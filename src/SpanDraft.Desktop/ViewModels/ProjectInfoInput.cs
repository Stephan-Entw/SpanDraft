using SpanDraft.Desktop.Resources;
using SpanDraft.Desktop.State;

namespace SpanDraft.Desktop.ViewModels;

/// <summary>Local setup text buffer; only confirmation changes the recoverable draft.</summary>
public sealed class ProjectInfoInput : ObservableObject
{
    private readonly ProjectSetupViewModel _owner;
    private readonly bool _required;
    private string _text;
    private bool _hasError;
    public ProjectInfoInput(ProjectSetupViewModel owner, string? value, bool required = false)
    {
        _owner = owner; _required = required;
        ConfirmedValue = ProjectInfo.Optional(value); _text = ConfirmedValue ?? "";
    }
    public string? ConfirmedValue { get; private set; }
    public bool IsEditing { get; private set; }
    public string Text
    {
        get => _text;
        set
        {
            if (!Set(ref _text, value)) return;
            IsEditing = true; HasError = false;
            _owner.RefreshInfo();
        }
    }
    public bool HasError { get => _hasError; private set => Set(ref _hasError, value); }
    public string ErrorText => Strings.RequiredProjectInfo;
    public void Begin() { if (!_owner.IsBusy) IsEditing = true; }
    public bool Confirm()
    {
        if (_owner.IsBusy) return false;
        if (_required && _owner.RequiresProjectInfo && string.IsNullOrWhiteSpace(Text))
        { HasError = true; return false; }
        var value = ProjectInfo.Optional(Text);
        bool changed = value != ConfirmedValue;
        ConfirmedValue = value; HasError = false;
        Text = value ?? ""; IsEditing = false;
        if (changed) _owner.InfoCommitted();
        return true;
    }
    public void Cancel()
    {
        if (_owner.IsBusy) return;
        HasError = false; Text = ConfirmedValue ?? ""; IsEditing = false;
    }
    public void LoseFocus()
    {
        if (_owner.IsBusy || _owner.Libraries?.PreserveSetupInputs == true || !IsEditing) return;
        Confirm();
    }
}
