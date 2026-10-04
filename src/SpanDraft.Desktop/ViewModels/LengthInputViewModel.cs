using SpanDraft.Core.Units;
using SpanDraft.Desktop.Resources;
using SpanDraft.Desktop.State;

namespace SpanDraft.Desktop.ViewModels;

public readonly record struct LengthCommitResult(bool Accepted, string? ErrorText = null)
{
    public static LengthCommitResult Success => new(true);
}

/// <summary>Transient input session for the single inline dimension editor.</summary>
public sealed class LengthInputViewModel(Func<Length> read, Func<Length, LengthCommitResult> commit) : ObservableObject
{
    private string _text = UiNumbers.Format(read().Millimeters);
    private bool _isEditing;
    private bool _hasError;
    private string? _commitError;
    private bool _isConfirming;

    public event Action<string>? BufferChanged;
    public event Action? EditCancelled;

    public string Text
    {
        get => _text;
        set
        {
            if (!Set(ref _text, value)) return;
            ClearError();
            if (IsEditing) BufferChanged?.Invoke(value);
        }
    }
    public bool IsEditing { get => _isEditing; private set { if (Set(ref _isEditing, value)) Notify(nameof(IsDisplay)); } }
    public bool IsDisplay => !IsEditing;
    public bool HasError { get => _hasError; private set => Set(ref _hasError, value); }
    public string ErrorText => _commitError ?? Strings.InvalidLength;
    public string DisplayText => UiNumbers.Format(read().Millimeters) + " mm";

    public void Begin()
    {
        if (IsEditing) return;
        Text = UiNumbers.Format(read().Millimeters);
        ClearError();
        IsEditing = true;
        BufferChanged?.Invoke(Text);
    }

    public bool Confirm()
    {
        if (!IsEditing || _isConfirming) return true;
        if (!UiNumbers.TryParseLength(Text, out Length length))
        {
            ClearError();
            HasError = true;
            return false;
        }
        // Preserve the input and its focus on rejection. Hiding it before validation
        // would emit focus loss and prevent a later real focus loss from restoring it.
        _isConfirming = true;
        try
        {
            var result = commit(length);
            if (!result.Accepted)
            {
                _commitError = result.ErrorText;
                HasError = true;
                Notify(nameof(ErrorText));
                return false;
            }
            IsEditing = false;
            Refresh();
            return true;
        }
        finally { _isConfirming = false; }
    }

    public void Cancel()
    {
        Restore();
        EditCancelled?.Invoke();
    }

    private void Restore()
    {
        IsEditing = false;
        Refresh();
    }

    public void LoseFocus()
    {
        if (_isConfirming || !IsEditing || Confirm()) return;
        // Leaving a rejected edit discards its request, conflict and feedback together.
        Cancel();
    }

    public void Refresh(bool preserveError = false)
    {
        if (!IsEditing) Text = UiNumbers.Format(read().Millimeters);
        if (!preserveError) ClearError();
        Notify(nameof(DisplayText));
    }

    private void ClearError()
    {
        _commitError = null;
        HasError = false;
        Notify(nameof(ErrorText));
    }
}
