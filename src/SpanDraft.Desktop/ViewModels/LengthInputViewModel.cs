using SpanDraft.Core.Units;
using SpanDraft.Desktop.Resources;
using SpanDraft.Desktop.Presentation;

namespace SpanDraft.Desktop.ViewModels;

public readonly record struct LengthCommitResult(bool Accepted, string? ErrorText = null)
{
    public static LengthCommitResult Success => new(true);
}

/// <summary>Transient input session for the single inline dimension editor.</summary>
public sealed class LengthInputViewModel(Func<Length?> read, Func<Length, LengthCommitResult> commit,
    Func<bool>? preserveBuffer = null, Func<UnitDefinition>? readUnit = null,
    bool retainRejectedOnBlur = false, Func<ResultPresentationOptions>? readPresentation = null) : ObservableObject
{
    private string _text = Format(read(), readUnit?.Invoke() ?? UnitCatalog.Millimeter);
    private UnitDefinition _editUnit = UnitCatalog.Millimeter;
    private Length? _referenceLength;
    private string _referenceText = "";
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
    private UnitDefinition CurrentUnit => IsEditing ? _editUnit : readUnit?.Invoke() ?? UnitCatalog.Millimeter;
    public string Unit => CurrentUnit.Symbol;
    public string DisplayText => read() is { } length ? QuantityFormatter.WithUnit(length.Meters, CurrentUnit, readPresentation?.Invoke()) : "";

    public bool TryGetLength(out Length length)
    {
        if (IsEditing && _referenceLength is { } reference && Text == _referenceText) { length = reference; return true; }
        if (!IsEditing && read() is { } current && Text == Format(current, CurrentUnit)) { length = current; return true; }
        return InputQuantityFormatter.TryParseLength(Text, CurrentUnit, out length);
    }

    public void Begin()
    {
        if (IsEditing) return;
        _editUnit = readUnit?.Invoke() ?? UnitCatalog.Millimeter;
        _referenceLength = read();
        Text = _referenceText = Format(_referenceLength, _editUnit);
        ClearError();
        IsEditing = true;
        Notify(nameof(Unit));
        BufferChanged?.Invoke(Text);
    }

    public bool Confirm()
    {
        if (!IsEditing || _isConfirming) return true;
        if (read() is null && string.IsNullOrWhiteSpace(Text))
        {
            ClearError();
            return false;
        }
        if (!TryGetLength(out Length length))
        {
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
        if (preserveBuffer?.Invoke() == true || _isConfirming || !IsEditing || Confirm()) return;
        // Leaving a rejected edit discards its request, conflict and feedback together.
        if (!retainRejectedOnBlur || (read() is null && string.IsNullOrWhiteSpace(Text))) Cancel();
    }

    public void Refresh(bool preserveError = false)
    {
        if (!IsEditing) Text = Format(read(), CurrentUnit);
        if (!preserveError) ClearError();
        Notify(nameof(DisplayText));
        Notify(nameof(Unit));
    }

    private static string Format(Length? length, UnitDefinition unit) =>
        length is { } value ? InputQuantityFormatter.FormatExact(value.Meters, unit) : "";

    private void ClearError()
    {
        _commitError = null;
        HasError = false;
        Notify(nameof(ErrorText));
    }
}
