using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Threading;
using Avalonia.VisualTree;
using SpanDraft.Desktop.ViewModels;

namespace SpanDraft.Desktop;

public partial class MaterialEditorWindow : Window
{
    private bool _completed;

    public MaterialEditorWindow() => InitializeComponent();

    /// <param name="shutdownAuthorized">True once a system shutdown closes every window: nothing may keep this one open.</param>
    public MaterialEditorWindow(MaterialEditorViewModel model, Func<bool>? shutdownAuthorized = null) : this()
    {
        DataContext = model;
        model.CloseRequested += Finish;
        Opened += (_, _) =>
        {
            if (Screens.ScreenFromWindow(this) is { } screen)
            {
                Width = Math.Min(Width, screen.WorkingArea.Width / screen.Scaling);
                MaxHeight = screen.WorkingArea.Height / screen.Scaling;
            }
            Dispatcher.UIThread.Post(() => this.GetVisualDescendants().OfType<TextBox>()
                .FirstOrDefault(t => t.Name == "MaterialNameInput")?.Focus(NavigationMethod.Tab));
        };
        Closing += (_, e) =>
        {
            if (_completed) return;
            // A platform shutdown only asks: the owner workspace decides; the editor is ended when it really closes.
            if (Workspaces.PlatformCloseRequest.IsPlatform(e) || shutdownAuthorized?.Invoke() == true) return;
            if (model.IsBusy) { e.Cancel = true; return; }
            _completed = true;
            model.CancelCommand.Execute(null);
        };
        Closed += (_, _) =>
        {
            model.CloseRequested -= Finish;
            if (_completed) return;
            _completed = true;
            model.CancelCommand.Execute(null);
        };
    }

    private void Finish(bool accepted)
    {
        if (_completed) return;
        _completed = true;
        Close(accepted);
    }
}
